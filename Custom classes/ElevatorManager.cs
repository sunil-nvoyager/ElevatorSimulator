using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;

namespace LiftSimulator
{
    /// <summary>    
    /// 1. Multithreading for elevators provided via ThreadPool class
    /// 2. Behaviour, when elevator was called:
    ///     - Use FindAllElevatorsWhichCanBeSent to pick elevators meeting any of following requirements:
    ///         - elevator is in its way to Passenger's floor (e.g. called by someone else)
    ///         - elevator is on different floor and its state is "Idle" 
    ///     - If list of available elevators is empty, do nothing
    /// 3. Manager has timer to periodcally (every 1000ms) check, if some floor doesn't need an elevator
    /// (which is signaled by Floor's LampUp and LampDown properties).
    /// </summary>
    public class ElevatorManager
    {
        #region FIELDS

        private readonly object locker = new object();
        private Elevator[] arrayOfAllElevators;
        public Elevator[] ArrayOfAllElevators
        {
            get;
            set;
        }

        private List<Elevator> listOfAllFreeElevators;

        private Floor[] arrayOfAllFloors;

        private System.Timers.Timer floorChecker;

        #endregion


        #region METHODS

        public ElevatorManager(Elevator[] ArrayOfAllElevators, Floor[] ArrayOfAllFloors)
        {
            
            //Initialize array with elevators
            this.arrayOfAllElevators = ArrayOfAllElevators;

            //Subscribe to elevators' events
            for (int i = 0; i < arrayOfAllElevators.Length; i++)
            {                
                arrayOfAllElevators[i].ElevatorIsFull += new EventHandler(ElevatorManager_ElevatorIsFull); //Subscribe to all ElevatorIsFull events
            }

            //Initialize array with floors
            this.arrayOfAllFloors = ArrayOfAllFloors;

            //Initialize list of free elevators
            this.listOfAllFreeElevators = new List<Elevator>();

            //Launch timer to periodically check, if some floor doesn't need an elevator
            this.floorChecker = new System.Timers.Timer(1000);
            this.floorChecker.Elapsed += new ElapsedEventHandler(this.ElevatorManager_TimerElapsed);
            this.floorChecker.Start();
            
        }

        /// <summary>
        /// Notifies the elevator system that a passenger needs an elevator from a specific floor and direction.
        /// </summary>
        /// <param name="PassengersFloor">The floor from which the passenger is requesting an elevator.</param>
        /// <param name="PassengersDirection">The direction in which the passenger wants to go (up or down).</param>
        /// <remarks>
        /// This method is responsible for handling the request for an elevator by a passenger. It first locks the operation to ensure thread safety, as it can be invoked from different threads, such as the ElevatorManager thread or its timer thread.
        /// Depending on the requested direction, it activates the appropriate lamp on the specified floor to indicate that an elevator is on its way.
        /// The method then searches for all elevators that can be dispatched to the passenger's floor and direction.
        /// After identifying potential elevators, it selects the optimal one to send based on predefined criteria.
        /// If a suitable elevator is found, it sends the elevator to the passenger's floor.
        /// This process ensures efficient handling of elevator requests in a multi-threaded environment.
        /// </remarks>
        public void PassengerNeedsAnElevator(Floor PassengersFloor, Direction PassengersDirection)
        {
            
            lock (locker)//Can be invoked from ElevatorManager thread or its timer thread
            {
                //Turn on appropriate lamp on the floor
                if (PassengersDirection == Direction.Up)
                {
                    PassengersFloor.LampUp = true;
                }
                else if (PassengersDirection == Direction.Down)
                {
                    PassengersFloor.LampDown = true;
                }

                //Search elevator
                FindAllElevatorsWhichCanBeSent(PassengersFloor, PassengersDirection);

                Elevator ElevatorToSend = ChooseOptimalElevatorToSend(PassengersFloor);

                if (ElevatorToSend != null)
                {
                    SendAnElevator(ElevatorToSend, PassengersFloor);
                }                
            }
            
        }

        /// <summary>
        /// Finds all elevators that can be sent to a specified floor in a given direction.
        /// </summary>
        /// <param name="PassengersFloor">The floor where the passengers are located.</param>
        /// <param name="PassengersDirection">The direction in which the passengers want to go.</param>
        /// <remarks>
        /// This method first clears the list of free elevators. It then checks if there are any elevators already on their way to the specified passenger's floor.
        /// If an elevator is found that is already en route, the method clears the list and returns immediately, indicating that no new elevator needs to be sent.
        /// If no elevators are on their way, it proceeds to check for elevators that are currently idle (not moving).
        /// All idle elevators are added to the list of free elevators, which can then be used to send an elevator to the passengers.
        /// This method effectively manages elevator dispatching by ensuring that only available elevators are considered for new requests.
        /// </remarks>
        private void FindAllElevatorsWhichCanBeSent(Floor PassengersFloor, Direction PassengersDirection)
        {
            
            listOfAllFreeElevators.Clear();

            //Find elevators in their way to Passenger's floor (e.g. called by someone else)
            for (int i = 0; i < arrayOfAllElevators.Length; i++)
            {
                //Get list of floors to visit
                List<Floor> ListOfFloorsToVisit = arrayOfAllElevators[i].GetListOfAllFloorsToVisit();

                //Check list of floors to visit                
                if (ListOfFloorsToVisit.Contains(PassengersFloor))
                {
                    listOfAllFreeElevators.Clear();
                    return; //Some elevator is already in its way, no need to send new one
                }
            }

            //Find elevators, which are idling now (do not moving anywhere)
            for (int i = 0; i < arrayOfAllElevators.Length; i++)
            {
                if (arrayOfAllElevators[i].GetElevatorStatus() == ElevatorStatus.Idle) 
                {
                    listOfAllFreeElevators.Add(arrayOfAllElevators[i]);
                }
            }
            
        }

        /// <summary>
        /// Chooses the optimal elevator to send based on the floor from which the call originated.
        /// </summary>
        /// <param name="FloorWhereTheCallCameFrom">The floor from which the elevator call was made.</param>
        /// <returns>The optimal elevator to send, or null if no elevators are available.</returns>
        /// <remarks>
        /// This method checks the list of all free elevators. If the list is empty, it returns null, indicating that there are no available elevators to respond to the call.
        /// If there are free elevators, it selects and returns the first elevator in the list.
        /// The logic for determining the "optimal" elevator could be expanded in the future to consider factors such as distance or current load.
        /// </remarks>
        private Elevator ChooseOptimalElevatorToSend(Floor FloorWhereTheCallCameFrom)
        {
            
            //Check if listOfAllFreeElevators is not empty
            if (listOfAllFreeElevators.Count == 0)
            {
                return null;
            }

            //Return first elevator from the list
            return listOfAllFreeElevators[0];     
            
        }

        /// <summary>
        /// Sends an elevator to a specified target floor.
        /// </summary>
        /// <param name="ElevatorToSend">The elevator that is to be sent to the target floor.</param>
        /// <param name="TargetFloor">The floor that the elevator should go to.</param>
        /// <remarks>
        /// This method adds the target floor to the list of floors for the specified elevator and then
        /// initiates a new thread to prepare the elevator for its journey to the next floor in the list.
        /// The use of a thread allows the elevator's preparation process to run asynchronously,
        /// ensuring that the main application remains responsive while the elevator is being dispatched.
        /// This method does not return any value and operates on the provided elevator instance directly.
        /// </remarks>
        private void SendAnElevator(Elevator ElevatorToSend, Floor TargetFloor)
        {            
            
            ElevatorToSend.AddNewFloorToTheList(TargetFloor);

            //Create new thread and send the elevator
            ThreadPool.QueueUserWorkItem(delegate { ElevatorToSend.PrepareElevatorToGoToNextFloorOnTheList(); });      
            
        }

        /// <summary>
        /// Handles the timer elapsed event for the elevator manager.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">An <see cref="ElapsedEventArgs"/> that contains the event data.</param>
        /// <remarks>
        /// This method checks each floor in the building to determine if there are passengers waiting for an elevator.
        /// It inspects the state of the lamps on each floor: if the 'LampUp' is activated, it indicates that a passenger needs an elevator to go up,
        /// and if the 'LampDown' is activated, it indicates a need to go down. For each active lamp, it calls the
        /// <see cref="PassengerNeedsAnElevator"/> method to handle the request, passing the corresponding floor and direction.
        /// A delay of 500 milliseconds is introduced after each request to prevent multiple elevators from being sent simultaneously.
        /// This method is typically invoked at regular intervals by a timer to manage elevator requests efficiently.
        /// </remarks>
        public void ElevatorManager_TimerElapsed(object sender, ElapsedEventArgs e)
        {
            
            //Check if some floor doesn't need an elevator
            for (int i = 0; i < arrayOfAllFloors.Length; i++)
                {
                    if (arrayOfAllFloors[i].LampUp)
                    {
                        PassengerNeedsAnElevator(arrayOfAllFloors[i], Direction.Up);
                        Thread.Sleep(500); //delay to avoid sending two elevators at a time
                    }
                    else if(arrayOfAllFloors[i].LampDown)
                    {
                        PassengerNeedsAnElevator(arrayOfAllFloors[i], Direction.Down);
                        Thread.Sleep(500); //delay to avoid sending two elevators at a time
                    }   
                }
            
        }

        public void ElevatorManager_ElevatorIsFull(object sender, EventArgs e)
        {    
            //TO DO: Implement or remove!
        }
        
        #endregion EVENT HANDLERS
    }
}
